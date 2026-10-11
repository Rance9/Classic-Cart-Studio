# Classic Cart Studio

**An independent game collection manager for the Nintendo NES Classic Edition.**

**Website:** [https://ClassicCartStudio.com](https://ClassicCartStudio.com) · **Windows App:** [https://ClassicCartStudio.com/app](https://ClassicCartStudio.com/app) (always the latest release)

Classic Cart Studio is one Studio in two places. The **website** lets you browse the NES game library, choose games and the exact versions you prefer, build and save collections, and read the complete Help & User Guide, with nothing to install. **Classic Cart Studio for Windows** shows that same live website in its own application window and adds everything that needs your computer: reading the games already set up on your PC, importing your own game files (ZIP archives included), keeping managed copies, and preparing the collection for Hakchi2 CE. A collection you build on the website continues in the Windows application with **Continue in Desktop App**; your selections, including the exact versions you chose, come with you.

> Classic Cart Studio is an independent, open-source project and is not produced, licensed, sponsored, approved, or endorsed by Nintendo. Nintendo, Nintendo Entertainment System, NES and NES Classic Edition are trademarks of Nintendo. Game titles, publisher names, artwork and box art are used to identify and present games; all rights remain with their owners. **No game files or console software are distributed by this project.**

## What you can do

- Browse a library of 773 curated North American releases plus an extended index of regional releases, revisions, translations and other versions, with version chips, technical details (mapper, header, hashes, emulator) and controller-compatibility notes.
- Build a collection: the 30 games that came with the console are already there; choose what stays visible and what you add. Save it to a file, load it later, keep automatic snapshots, and restore.
- Continue in the Windows application: the collection you built on the website is handed over as it is (names, exact ROM versions, factory-game choices). The application then helps you find the matching game files on your computer: individual files, several files, ZIP archives or whole folders, every file identified by its exact content, never by file name.
- Add your own homebrew with artwork, load the games already set up on your computer (an existing Hakchi2 CE library), and prepare the collection for Hakchi2 CE.
- Installation on the console is done by Hakchi2 CE under your control. This version prepares everything on the PC and shows the console steps honestly as not automated.

## Website vs Windows application

| | Website only | Classic Cart Studio for Windows |
|---|---|---|
| Browse, filter, plan, save/load collections, read the guide | yes | yes (the same live website, in its own window) |
| Continue a website collection without starting over | — | yes (Continue in Desktop App) |
| Read the games already on your computer | no | yes |
| Add games from files / folders / ZIPs | no | yes |
| Prepare a collection for Hakchi2 CE, open Hakchi | no | yes |
| Anything sent to the console by this software | never | never |

The Windows application starts a small backend that listens only on your own computer (`127.0.0.1`). The application window talks to it over a session that exists only while the application runs; there is no pairing step inside the application. The public website never connects to that backend on its own: when you choose something that needs your computer, it hands your collection to the application (Edge asks once whether the website may access apps on your device; see the guide chapter *Continuing in the Desktop App*), or sends you to [ClassicCartStudio.com/app](https://ClassicCartStudio.com/app) to install it.

## Installation (Windows 10/11)

1. On the website, choose **Continue in Desktop App** (My Collection menu or Install on NES Classic) or go to [ClassicCartStudio.com/app](https://ClassicCartStudio.com/app), which always opens the latest release (the full [release history](https://github.com/Rance9/Classic-Cart-Studio/releases) is on GitHub).
2. Download `ClassicCartStudio-Setup-<version>.exe`.
3. **Check it** (recommended): in PowerShell run
   `Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\Downloads\ClassicCartStudio-Setup-<version>.exe"`
   and compare the result with `SHA256SUMS.txt` on the release (the website shows the same value with a Copy button).
4. Run the installer. It installs for your user account only (no administrator rights), under `%LOCALAPPDATA%\Programs\NESClassicCollectionStudio`, and adds a Start-menu entry **Classic Cart Studio**.
5. Start Classic Cart Studio from the Start menu. It opens the Studio in its own window with the backend already connected. If you came from the website with Continue in Desktop App, the application shows the collection and asks before opening it.

Classic Cart Studio needs the Microsoft Edge WebView2 runtime, which is part of Windows 10/11 with Microsoft Edge, and .NET Framework 4.8, which is part of Windows 10 (version 1903 and later) and Windows 11. An internet connection is needed to load the website inside the window; if the website cannot be reached the application shows a recovery screen and keeps your data.

**Upgrading from the earlier "NES Classic Collection Studio Companion" releases (0.1.x)**: run the new installer; it replaces the program in the same folder and keeps your data (imported games, personal games, collections, Hakchi setup) in `%LOCALAPPDATA%\NESClassicCollectionStudio`. The Start-menu entry is now Classic Cart Studio. **Uninstalling**: Windows Settings → Apps. Your data folder stays until you delete it yourself.

### Why the installer is not signed

Windows code-signing certificates cost money and bind a legal identity; this project does not have one yet, so Windows SmartScreen shows "unknown publisher" on first run. What you get instead: every release is built by a public GitHub Actions workflow from the tagged source, the workflow run is linked from the release notes, a build-provenance attestation is attached, and the SHA-256 of the installer is published. If the hash you compute does not match, do not run the file. A matching hash proves the download is the file the workflow built; it does not by itself prove who published it, which is what the attestation and the public workflow are for.

### Hakchi2 CE and the console driver

Preparing a collection needs [Hakchi2 CE](https://github.com/TeamShinkansen/Hakchi2-CE) 3.9.3 (GPL v3, a separate program). The application can set it up for you: it downloads the official release package from the Hakchi2 CE GitHub releases page, verifies its size and SHA-256 against the values pinned in `tools/companion/hakchi_bridge.py`, and extracts the portable build. It can also run Hakchi's bundled console USB driver installer (that one step asks for administrator rights, because installing a driver requires it). Nothing is written to the console by this project.

## Your game files

The Studio never downloads games and never ships them. It works with files **you already own**, for example dumps of your own cartridges. When you add files, the application keeps its own copy and never modifies, moves or deletes your original. Games that are in the library but have no file on your computer are shown as **missing**: use **Find My Game Files** or **Locate Game File…** to point the Studio at your copy; it accepts the file only if its content is exactly that version (no substitutions). The Internet Archive and similar sites are referenced in the documentation only as *provenance* of public identification data (hashes, names); this project includes no downloader and does not encourage downloading copyrighted games.

## Privacy

The website stores your collection, favorites and settings in your browser only. The Windows application keeps its data in `%LOCALAPPDATA%\NESClassicCollectionStudio` on your PC (including the durable copy of your collection state and the WebView2 profile). Nothing is sent to any server by this project; the website and the application fetch only the website's own files and the public GitHub releases information. Details: [PRIVACY.md](PRIVACY.md).

## Development

```
python -m pip install pillow
python tools/publish/build_site_ci.py build/site          # static website from site-data/ (data, artwork, fonts)
python tools/rom-pipeline/test_public_site.py build/site  # page checks
python tools/companion/nes_companion.py --port 8765       # the backend from source (serves the site you built: set NES_STUDIO_SITE)
dotnet build tools/desktop/ClassicCartStudio -c Release   # Classic Cart Studio for Windows (WebView2 host, .NET Framework 4.8)
python tools/companion/test_handoff.py                    # desktop channel + handoff checks (Python only)
python tools/desktop/poc_run.py --site build/site         # the host against the backend from source (CCS_DEV), DevTools-driven
```

The backend from source: `set NES_STUDIO_SITE=build\site` then run `nes_companion.py`; the website at `http://localhost:4173` (or any loopback origin you list under `allowed_origins` in `companion.json`) can then pair with it. The public website is allowed by default. The host in development mode (`CCS_DEV=1`, `CCS_SITE_URL`, `CCS_BACKEND_SOURCE`) runs the backend from source and may show a local preview origin instead of the website.

Packaging (what the release workflow does): `pyinstaller tools/companion/companion.spec` (the backend, one folder), `dotnet build` of the host with the release version, the host's files copied beside the backend, `ClassicCartStudio.exe --selftest`, then Inno Setup with `tools/companion/installer.iss`.

Layout: `tools/rom-pipeline/` (data model, page generator, Studio front-end in `studio/`), `tools/companion/` (loopback backend, Hakchi bridge, inspector, import, legacy status window, installer script), `tools/desktop/` (the Windows host and its harnesses), `tools/publish/` (public build, content scanner, release helpers), `site-data/` (public identification data: titles, publishers, hashes; no files), `.github/workflows/` (website deploy, Windows release).

## Support

Classic Cart Studio is free and open source. If you've found it useful, optional contributions through [GitHub Sponsors](https://github.com/sponsors/Rance9) are always appreciated and help support continued independent development. Nothing in the Studio depends on it: no paid features, no gates, no prompts.

The console the creator used was bought from an eBay seller recommended in the guide's "Getting an NES Classic" chapter. That is a personal recommendation, not a sponsored listing or affiliate relationship: no commission or referral compensation is received, and it is independent of GitHub Sponsors.

## Credits and licenses

- This project's code: MIT (see `LICENSE`).
- Pixel artwork and interface icons: Nintendo's renderings from the NES Classic Edition system software and documented community sprite rips (origins in the site footer); box art from community thumbnail collections. Shown to identify and present the games; all rights remain with their owners; not covered by the MIT license.
- [Hakchi2 CE](https://github.com/TeamShinkansen/Hakchi2-CE) by TeamShinkansen and contributors (GPL v3) does the console side; it is not bundled.
- Microsoft Edge WebView2 (runtime and SDK) displays the website inside the Windows application; .NET Framework 4.8 runs the host.
- "Press Start 2P" font: SIL Open Font License.
- Game identification data compiled from No-Intro, NesCartDB and Mesen database releases.

## Legal, privacy and third-party notices

- [LICENSE](LICENSE): MIT for this project's own code.
- [PRIVACY.md](PRIVACY.md): what the website and the Windows application store and transmit.
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md): Hakchi2 CE, WebView2, fonts, identification databases, artwork and build tools, with what is bundled and what is downloaded separately.
- The same information, in plain words, is in the Studio's Help & User Guide → Legal, Privacy & Disclaimers.

## Security

See `SECURITY.md` for what the Windows application and its backend do, how to verify downloads and how to report a problem.
