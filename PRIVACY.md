# Privacy

Classic Cart Studio consists of a website (static files served by GitHub Pages at https://classiccartstudio.com/) and an optional Windows application, **Classic Cart Studio for Windows**, which shows that same website in its own window and runs a local backend. This document describes what each part stores and transmits, based on the code in this repository. There is no account, no analytics and no telemetry in either part.

## The website

**Stored in your browser only** (localStorage of the site's origin; nothing is sent anywhere):

- your collection (selected games, version decisions, name, status, import baseline), automatic draft and snapshots (`studio-collection-v1`, `studio-snapshots-v1`, legacy `nes-classic-selection-v1`, `nes-classic-decisions-v1`);
- favorites, list/grid view, Extended Library switch, first-run flag (`nes-classic-favorites-v1`, `nes-classic-view-v1`, `studio-ext-v1`, `studio-welcomed-v1`);
- the Preview on TV display treatment (`studio-tv-v1`: clean or retro),
- the id of the last collection handed to the desktop application (`studio-handoff-v1`, an identifier only). The website holds no token for the backend on your computer: it never connects to it directly.

You can remove all of it by clearing the site's data in your browser. "Export Studio Data" writes the same information to a file you keep; "Import Studio Data" reads such a file. A browser profile and the Windows application's window have separate storage; the handoff below exists so you do not have to start over.

**Network requests the website makes:**

- its own files from `https://classiccartstudio.com` (GitHub Pages serves them; GitHub's privacy statement applies to that hosting; the former address `https://rance9.github.io/Classic-Cart-Studio/` redirects there);
- `release.json` from the same site and, as a fallback for the download section, the public GitHub Releases API at `https://api.github.com` (unauthenticated);
- the backend on your own computer at `http://127.0.0.1:8765`, only after you connect it or choose **Continue in Desktop App** (see below).

Nothing else is contacted. No cookies are set by the project.

## Continue in Desktop App (website → application)

When you choose **Continue in Desktop App**, the website sends your current collection (game identifiers, chosen versions, factory-game choices, the collection name; never game files) to the backend on your own computer at `127.0.0.1`, and opens the application through the `nesstudio://` link, which carries only an identifier. Microsoft Edge may ask whether the website may access apps on your device; that is the browser's local-network permission for exactly this. The backend accepts such offers only from the website's exact origin, keeps at most a few of them for 20 minutes in `handoff-inbox.json`, and the application asks you before opening one. Declining the browser permission or the application prompt loses nothing: the collection stays in your browser, and **Download Collection & Open in App** gives you a file instead. Nothing leaves your computer in this process.

## Classic Cart Studio for Windows

**Stored on your PC** under `%LOCALAPPDATA%\NESClassicCollectionStudio` (the same folder the earlier companion used, so an upgrade keeps your data):

- `personal\`: copies of game files you import (managed copies of known versions and your personal games), their artwork, and `index.json` / `library-store.json` describing them. Your original files are never modified, moved or deleted.
- `desktop-state.json` (and `.bak`): the application's durable copy of your collection, favorites, snapshots and display settings, written through the backend so it survives a WebView2 profile reset or an upgrade.
- `webview2\`: the application window's own browser profile (its localStorage, cache and cookies for the website).
- `handoff-inbox.json`: collections offered by the website that you have not yet opened or discarded (expire after 20 minutes).
- `exports\`: collection files saved during preparation. `hakchi-staging\`: a backup, a journal and a ledger for every preparation run, so a run can be undone.
- `logs\desktop.log`, `logs\companion.log`, `logs\server.log`: operational logs (startup, requests by path, handoff events by origin, errors). They never contain tokens.
- `companion.json` (settings: port, paths, optional loopback development origins) and `desktop.json` (window size and position).

The backend reads the Hakchi2 CE folder you configured (games and config) when you ask it to (Load Existing Collection, Check, Prepare). It does not scan other folders or your whole disk; imports examine only the files or folder you pick.

**Network:** the backend listens only on `127.0.0.1` (your own computer); it never opens a network port and never contacts the console. The application window loads the website from `https://classiccartstudio.com` and the release information like any browser would. The only other outbound download happens on your explicit request: the pinned Hakchi2 CE 3.9.3 release package from the Hakchi2 CE GitHub releases page, verified by size and SHA-256 before use. The application does not check for or install updates by itself; the Studio menu's "Check for updates" opens the public releases page in your browser.

**Authorization:** the application window talks to its backend with a session created when the application starts and discarded when it closes (no pairing step, nothing stored). The website in your normal browser never uses the backend's privileged requests; the only thing it may send is a collection handoff (see above), and only from `https://classiccartstudio.com` (and `www`, and the older `https://rance9.github.io` address).

**Browser permission:** when you choose Continue in Desktop App, Microsoft Edge and Chrome ask once whether the website may reach programs on your local network (the handoff goes to 127.0.0.1). Declining it only prevents the direct handoff; Download Collection & Open in App works without it, and the website keeps working on its own.

## Downloads and updates

The website shows the latest release (version, size, SHA-256) from `release.json` or the GitHub Releases API so you can verify a download; nothing identifies you in that request beyond what any HTTPS request carries. Updates are installed only by you, by running a newer installer; your data folder is kept.

## Storage estimate

The "NES Classic · estimated game space" figure is a reference estimate derived from the documented storage layout of a stock 2018 NES Classic Edition. It is not read from your console and is not personal data.

## Removal

Uninstalling Classic Cart Studio (Windows Settings → Apps) removes the program and keeps the data folder above so your imported games and collections survive a reinstall; delete `%LOCALAPPDATA%\NESClassicCollectionStudio` yourself to remove them, including the application's browser profile. Clear the site's browser data to remove everything the website kept in your normal browser.

## Contact

Questions and reports: the repository's issue tracker at https://github.com/Rance9/Classic-Cart-Studio/issues (public). For security matters see `SECURITY.md`.
