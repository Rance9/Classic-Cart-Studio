# Privacy

Classic Cart Studio has two parts: the website at https://classiccartstudio.com/ (static files hosted on GitHub Pages) and the optional Windows app, **Classic Cart Studio for Windows**. This document describes what each part stores and sends, based on the code in this repository. There is no account, no analytics and no telemetry.

## The website

**Stored in your browser only** (the site's local storage; nothing is sent anywhere):

- your collections, their NES Classic Loadouts, HOME Setup choices, version decisions and snapshots;
- favorites and display settings (list or grid view, technical columns, the Extended Library switch, the Preview on TV look, the first-run flag);
- the identifier of the last collection handed to the Windows app (an identifier only).

Clear the site's data in your browser to remove all of it. The website does not store ROM files: when you share or export a collection, it reads the ROM files (or the `.ccs` file) you choose for that moment only.

**Network requests the website makes:**

- its own files from `https://classiccartstudio.com` (hosted by GitHub Pages; GitHub's privacy statement applies);
- `release.json` from the same site and, as a fallback for the download section, the public GitHub Releases API at `https://api.github.com`;
- the Windows app on your own computer at `http://127.0.0.1:8765`, only when you choose **Open in Windows App** (see below).

Nothing else is contacted. The project sets no cookies.

## Sharing and exporting (.ccs files)

A `.ccs` file is a complete collection: every game with its ROM file, plus the collection's loadouts, settings and your own artwork. **Export .ccs**, **Send to Phone** and **Share Games** make that file on your own device. Classic Cart Studio never uploads a `.ccs` file or any ROM file, and runs no service that receives, stores, relays or processes them, not even briefly.

When you choose **Send to Phone** or **Share Games**, the file goes to the app you pick in your device's own share sheet (for example Messages, Mail, AirDrop, OneDrive, Phone Link or Nearby Share). That app sends it the way it normally does, under its own terms; Classic Cart Studio is not involved in that transfer. **Export .ccs** saves the file where you choose.

Importing a `.ccs` file also happens entirely on your device: the website checks every ROM file in it and does not keep them; the Windows app adds them to its own folder only after you confirm.

## Open in Windows App (website → app)

When you choose **Open in Windows App**, the website sends the collection (or the `.ccs` package you picked) to the Windows app on your own computer at `127.0.0.1`, then opens the app through a `classiccartstudio://` link that carries only an identifier. Your browser may ask whether the website may reach apps on your device; that is the browser's local-network permission for exactly this step. The app accepts such offers only from the website's own address, keeps them for a short time, and asks you before opening one. Declining loses nothing: your collection stays in your browser, and **Export** gives you a file to open instead. Nothing leaves your computer in this step.

## Classic Cart Studio for Windows

The app's interface is installed with it and served from your own computer; it does not load the website to run.

**Stored on your PC** under `%LOCALAPPDATA%\ClassicCartStudio`:

- copies of ROM files you import, their artwork, and the index that describes them (your original files are never changed, moved or deleted);
- your collections, loadouts, favorites, snapshots and settings, saved so they survive an upgrade;
- the window's own browser profile;
- collections and packages offered by the website or opened from a `.ccs` file that you have not yet opened or discarded (they expire after a day or less);
- a backup and a journal for every preparation run, so a run can be undone;
- logs of startup, requests, handoffs and errors (never passwords or keys), and the window size and position.

The app reads the Hakchi2 CE folder only when you ask it to (Check, Prepare) and examines only the files or folders you pick for import. It does not scan your disk.

**Network:** the app's local service listens only on `127.0.0.1` (your own computer) and never contacts the console. Hakchi2 CE 3.9.3 is included with the app and verified by SHA-256 before use. The app contacts the internet only when you ask it to: **Check for Updates**, which opens the releases page in your browser. Sharing a `.ccs` file uses the app you pick, as described above. The app never checks for or installs updates by itself.

**Authorization:** the app's window talks to the local service with a session created when the app starts and discarded when it closes. A website in your normal browser can only hand over a collection or a package as described above, and only from `https://classiccartstudio.com`.

## Downloads and updates

The website shows the latest release (version, size, SHA-256) so you can verify a download. Updates are installed only by you, by running a newer installer; your data folder is kept.

## Storage estimate

The storage figures in the loadout editor are estimates for a stock 2018 NES Classic Edition. They are not read from your console and are not personal data.

## Removal

Uninstalling Classic Cart Studio (Windows Settings → Apps) removes the program and keeps the data folder above, so your imported ROM files and collections survive a reinstall. Delete `%LOCALAPPDATA%\ClassicCartStudio` yourself to remove them. Clear the site's data in your browser to remove what the website kept.

## Contact

Questions and reports: the repository's issue tracker at https://github.com/Rance9/Classic-Cart-Studio/issues (public). For security matters, see `SECURITY.md`.
