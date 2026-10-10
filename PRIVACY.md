# Privacy

NES Classic Collection Studio consists of a static website (GitHub Pages) and an optional Windows companion program. This document describes what each part stores and transmits, based on the code in this repository. There is no account, no analytics and no telemetry in either part.

## The website

**Stored in your browser only** (localStorage of the site's origin; nothing is sent anywhere):

- your collection (selected games, version decisions, name, status, import baseline), automatic draft and snapshots (`studio-collection-v1`, `studio-snapshots-v1`, legacy `nes-classic-selection-v1`, `nes-classic-decisions-v1`);
- favorites, list/grid view, Extended Library switch, first-run flag (`nes-classic-favorites-v1`, `nes-classic-view-v1`, `studio-ext-v1`, `studio-welcomed-v1`);
- the pairing token for the companion on your computer (`studio-companion-v1`), which is bound to the website's origin and expires after 12 hours.

You can remove all of it by clearing the site's data in your browser. "Export Studio Data" writes the same information to a file you keep; "Import Studio Data" reads such a file.

**Network requests the website makes:**

- its own files from `https://rance9.github.io` (GitHub Pages serves them; GitHub's privacy statement applies to that hosting);
- `release.json` from the same site and, as a fallback for the download section, the public GitHub Releases API at `https://api.github.com` (unauthenticated);
- the companion on your own computer at `http://127.0.0.1:8765`, only after you connect it (see below).

Nothing else is contacted. No cookies are set by the project.

## The companion (Windows)

**Stored on your PC** under `%LOCALAPPDATA%\NESClassicCollectionStudio`:

- `personal\`: copies of game files you import (managed copies of known versions and your personal games), their artwork, and `index.json` / `library-store.json` describing them. Your original files are never modified, moved or deleted.
- `exports\`: collection files saved during preparation.
- `hakchi-staging\`: a backup, a journal and a ledger for every preparation run, so a run can be undone.
- `logs\companion.log`: operational log (requests, pairing events by origin, errors). It never contains pairing tokens.
- `companion.json`: settings (port, paths, optional loopback development origins).

The companion reads the Hakchi2 CE folder you configured (games and config) when you ask it to (Load Existing Collection, Check, Prepare). It does not scan other folders or your whole disk; imports examine only the files or folder you pick.

**Network:** the companion listens only on `127.0.0.1` (your own computer). It never opens a network port and never contacts the console. It makes exactly one outbound download, on your explicit request: the pinned Hakchi2 CE 3.9.3 release package from the Hakchi2 CE GitHub releases page, verified by size and SHA-256 before use.

**Pairing:** a website may use the companion only after you click Allow in the companion's own window; the grant is bound to that website's exact origin, lasts 12 hours, lives only in memory, and is revoked by "Disconnect this website" on the site, "Disconnect all websites" in the companion window, or closing the companion. Only `https://rance9.github.io` and loopback development origins you list in `companion.json` are accepted.

**Browser permission:** Microsoft Edge and Chrome ask once whether the website may reach programs on your local network. Declining it only prevents the connection; the website keeps working on its own.

## Downloads and updates

The companion does not check for updates by itself. The website shows the latest release (version, size, SHA-256) from `release.json` or the GitHub Releases API so you can verify a download; nothing identifies you in that request beyond what any HTTPS request carries.

## Storage estimate

The "NES Classic · estimated game space" figure is a reference estimate derived from the documented storage layout of a stock 2018 NES Classic Edition. It is not read from your console and is not personal data.

## Removal

Uninstalling the companion (Windows Settings → Apps) removes the program and keeps the data folder above so your imported games and collections survive a reinstall; delete `%LOCALAPPDATA%\NESClassicCollectionStudio` yourself to remove them. Clear the site's browser data to remove everything the website kept.

## Contact

Questions and reports: the repository's issue tracker at https://github.com/Rance9/NES-Classic-Collection-Studio/issues (public). For security matters see `SECURITY.md`.
