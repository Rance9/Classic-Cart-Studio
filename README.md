# NES Classic Collection Studio

**An independent game collection manager for the Nintendo NES Classic Edition.**

Website: **https://rance9.github.io/NES-Classic-Collection-Studio/** · Releases (Windows companion): **https://github.com/Rance9/NES-Classic-Collection-Studio/releases**

The Studio is a website for choosing, organising and checking a collection of NES games for an NES Classic Edition, and a small Windows **companion** program that does the work that a website cannot do by itself: read the games already set up on your computer, keep the game files you import, prepare your collection and open the console-management software (Hakchi2 CE). The website works on its own for browsing and planning; the companion adds everything that touches your files.

> NES Classic Collection Studio is an independent project. It is not produced, licensed, sponsored, approved or endorsed by Nintendo. Nintendo, Nintendo Entertainment System, NES and NES Classic Edition are trademarks of Nintendo. Game titles, publisher names, artwork and box art are used to identify and present games; all rights remain with their owners. **No game files or console software are distributed by this project.**

## What you can do

- Browse a library of 773 curated North American releases plus an extended index of regional releases, revisions, translations and other versions, with version chips, technical details (mapper, header, hashes, emulator) and controller-compatibility notes.
- Build a collection: the 30 games that came with the console are already there; choose what stays visible and what you add. Save it to a file, load it later, keep automatic snapshots, and restore.
- With the companion: load the games already set up on your computer, **add games from files or whole folders** (ZIP archives included; everything identified by exact content, never by file name), add your own homebrew with artwork, and prepare the collection for Hakchi2 CE.
- Installation on the console is done by Hakchi2 CE under your control. This version of the Studio prepares everything on the PC and shows the console steps honestly as not automated.

## Website vs companion

| | Website only | Website + companion |
|---|---|---|
| Browse, filter, plan, save/load collections | yes | yes |
| Read the games already on your computer | no | yes |
| Add games from files / folders / ZIPs | no | yes |
| Prepare a collection for Hakchi2 CE, open Hakchi | no | yes |
| Anything sent to the console by this software | never | never |

The companion listens only on your own computer (`127.0.0.1`). The website asks for permission once per session (you click **Allow** in the companion's window); you can disconnect at any time.

## Installation (Windows 10/11)

1. On the website, click **Connect to Your Computer** → **Get the companion**, or open the [releases page](https://github.com/Rance9/NES-Classic-Collection-Studio/releases).
2. Download `NESClassicCollectionStudio-Setup-<version>.exe`.
3. **Check it** (recommended): in PowerShell run
   `Get-FileHash -Algorithm SHA256 "$env:USERPROFILE\Downloads\NESClassicCollectionStudio-Setup-<version>.exe"`
   and compare the result with `SHA256SUMS.txt` on the release (the website shows the same value with a Copy button).
4. Run the installer. It installs for your user account only (no administrator rights), under `%LOCALAPPDATA%\Programs\NESClassicCollectionStudio`, and adds a Start-menu entry.
5. Start **NES Classic Collection Studio Companion**, go back to the website and click **Connect to Your Computer** → **Connect** → **Allow**.

**Upgrading**: run the newer installer; it replaces the program and keeps your data. **Uninstalling**: Windows Settings → Apps. Your imported games, personal games and collections stay in `%LOCALAPPDATA%\NESClassicCollectionStudio` until you delete that folder yourself.

### Why the installer is not signed

Windows code-signing certificates cost money and bind a legal identity; this project does not have one yet, so Windows SmartScreen shows "unknown publisher" on first run. What you get instead: every release is built by a public GitHub Actions workflow from the tagged source, the workflow run is linked from the release notes, a build-provenance attestation is attached, and the SHA-256 of the installer is published. If the hash you compute does not match, do not run the file.

### Hakchi2 CE and the console driver

Preparing a collection needs [Hakchi2 CE](https://github.com/TeamShinkansen/Hakchi2-CE) 3.9.3 (GPL v3, a separate program). The companion can set it up for you: it downloads the official release package from the Hakchi2 CE GitHub releases page, verifies its size and SHA-256 against the values pinned in `tools/companion/hakchi_bridge.py`, and extracts the portable build. It can also run Hakchi's bundled console USB driver installer (that one step asks for administrator rights, because installing a driver requires it). Nothing is written to the console by this project.

## Your game files

The Studio never downloads games and never ships them. It works with files **you already own**, for example dumps of your own cartridges. When you add files, the companion keeps its own copy and never modifies, moves or deletes your original. Games that are in the library but have no file on your computer are shown as **missing**: use **Locate Game File…** to point the Studio at your copy; it accepts the file only if its content is exactly that version (no substitutions). The Internet Archive and similar sites are referenced in the documentation only as *provenance* of public identification data (hashes, names); this project includes no downloader and does not encourage downloading copyrighted games.

## Privacy

The website stores your collection, favourites and settings in your browser only. The companion keeps its data in `%LOCALAPPDATA%\NESClassicCollectionStudio` on your PC. Nothing is sent to any server by this project; the website fetches only its own files and the public GitHub releases information for the download section.

## Development

```
python -m pip install pillow
python tools/publish/build_site_ci.py build/site          # static website from site-data/ (data, artwork, fonts)
python tools/rom-pipeline/test_public_site.py build/site  # page checks
python tools/companion/nes_companion.py --port 8765       # the companion from source (serves the site you built: set NES_STUDIO_SITE)
```

The companion from source: `set NES_STUDIO_SITE=build\site` then run `nes_companion.py`; the website at `http://localhost:4173` (or any loopback origin you list under `allowed_origins` in `companion.json`) can then pair with it. The public website is allowed by default.

Packaging (what the release workflow does): `pyinstaller tools/companion/companion.spec`, then Inno Setup with `tools/companion/installer.iss`.

Layout: `tools/rom-pipeline/` (data model, page generator, Studio front-end in `studio/`), `tools/companion/` (loopback server, Hakchi bridge, inspector, import, desktop app, installer script), `tools/publish/` (public build, content scanner, sprites, release helpers), `site-data/` (public identification data: titles, publishers, hashes; no files), `.github/workflows/` (website deploy, companion release).

## Support

NES Classic Collection Studio is free and open source. If you've found it useful, optional contributions through [GitHub Sponsors](https://github.com/sponsors/Rance9) are always appreciated and help support continued independent development. Nothing in the Studio depends on it: no paid features, no gates, no prompts.

The console the creator used was bought from an eBay seller recommended in the guide's "Getting an NES Classic" chapter. That is a personal recommendation, not a sponsored listing or affiliate relationship: no commission or referral compensation is received, and it is independent of GitHub Sponsors.

## Credits and licences

- This project's code: MIT (see `LICENSE`).
- Pixel artwork and interface icons: Nintendo's renderings from the NES Classic Edition system software and documented community sprite rips (origins in the site footer); box art from community thumbnail collections. Shown to identify and present the games; all rights remain with their owners; not covered by the MIT licence.
- [Hakchi2 CE](https://github.com/TeamShinkansen/Hakchi2-CE) by TeamShinkansen and contributors (GPL v3) does the console side; it is not bundled.
- "Press Start 2P" font: SIL Open Font License.
- Game identification data compiled from No-Intro, NesCartDB and Mesen database releases.

## Security

See `SECURITY.md` for what the companion does, how to verify downloads and how to report a problem.
