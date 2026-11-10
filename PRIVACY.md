# Privacy

Classic Cart Studio has two parts: the website at https://classiccartstudio.com/ (which can also be installed as an app from the browser) and the optional Windows app, **Classic Cart Studio for Windows**. This notice describes what each part stores and what it sends, based on the code in this repository for this release. There is no account and no advertising.

## The website (and the app installed from it)

**Stored in your browser, for this website only.** None of it is a folder you can open, and none of it is uploaded:

- your collections, their NES Classic Loadouts, HOME Setup choices, version decisions and snapshots;
- favorites, Game Settings (Rapid Fire and Gameplay Defaults), display settings, the HOME Music choice and the identifier of the last collection handed to the Windows app (an identifier only);
- when you import a `.ccs` package or choose a game's file to play it, a private copy of that game file (checked by its content), so it can be played again;
- the saved games of games played here: the four slots, the last used save and the cartridge save of each game, with controller setups;
- after you install the website as an app or keep it ready for offline use, a copy of the website itself (pages, data, the game player, artwork and music) so it opens offline. It never reads or changes your games or saved games.

**How long it lasts.** This storage belongs to your browser. Clearing the site's data, private browsing, low storage or a long time without use can remove it; the website asks the browser to keep it, but the browser decides. On an iPhone, Safari and the app added to the Home Screen keep **separate** stores: something added in one does not appear in the other. Keep your `.ccs` backups (or your own game files): importing a backup again, or restoring a Full Studio Backup, brings the games and their saved games back.

**Network requests the website makes:**

- its own files from `https://classiccartstudio.com` (hosted by GitHub Pages and delivered through Cloudflare's network; their privacy statements apply). As with any website, the services that deliver it receive your device's IP address and browser details to answer the request;
- `release.json` from the same site and, as a fallback for the download section, the public GitHub Releases API at `https://api.github.com`;
- **Cloudflare Web Analytics**, added to the website by its host network: a small script from `static.cloudflareinsights.com` counts page views, visits, referring sites, device and browser types and page-load performance. Cloudflare describes it as using no cookies and not following people across websites. It is part of the website only; the Windows app's own interface does not include it;
- **only if you turn on Usage Reporting** (see below): a few counts to Classic Cart Studio's collector at `https://metrics.classiccartstudio.com`;
- the Windows app on your own computer at `http://127.0.0.1:8765`, only when you choose **Open in Windows App** (see below).

Links you choose (the merch store, GitHub, reference sites) open in your browser only when you click them. The project sets no cookies.

## Optional usage reporting

Classic Cart Studio can send a few counts that show which parts of it are used and where they fail. **It is optional:** every feature works the same with it on or off, offline too, and with the collector blocked. Nothing about it is needed to play, save, back up or restore.

- **The choice:** at any time in the Studio menu › **Usage Reporting**.
  - **Website and installed web app:** nothing is sent until you turn it on; no choice means nothing is sent.
  - **Windows app:** setup asks on its own page, separate from the Installation Terms ("Help improve Classic Cart Studio with basic usage and error reports"). The box starts unchecked: nothing is sent unless you check it. You decide before setup continues, and setup saves your answer on this computer before the app first starts. Setup itself sends nothing. When setup finds a choice already saved on this computer (an upgrade, repair or reinstall), it keeps that choice and does not ask again. A silent installation asks nothing and saves nothing. Without a saved choice, the app sends nothing.
  - The choice belongs to one device and one browser: on an iPhone, Safari and the app added to the Home Screen each have their own. It is never part of a backup, and a restore never changes it.
- **What a count says, and nothing more:** that the Studio opened, a HOME opened, a game started, a backup or restore finished, a download link was chosen, or something failed (with a fixed category such as "missing ROM" or "write failed"); the release version; whether it came from the website, the installed web app or the Windows app; the browser family (Chrome, Edge, Firefox, Safari or other) and system family (Windows, macOS, iOS, Android, Linux or other); and, for some failures, a rough duration.
- **Never included:** game files or their hashes, game names, collection or loadout names, file names or paths, saved games, screenshots, controller details, typed text, error messages, web addresses, or any account, device, installation or advertising identifier.
- **Where it goes:** Classic Cart Studio's own collector, a Cloudflare Worker at `https://metrics.classiccartstudio.com`, which stores the count in Cloudflare Workers Analytics Engine. The collector does not store your IP address, browser details or the request itself. Cloudflare, as the network that delivers the request, necessarily processes the connection's IP address and request details under its own terms.
- **How long:** Cloudflare Workers Analytics Engine keeps the counts for three months. There is no other copy.
- **Turning it off** stops new counts at once, in every open page of the same browser too; anything not yet sent is dropped (a count already delivered cannot be recalled). If the choice cannot be saved, reporting stays off until that page or the app closes, and Classic Cart Studio says so. A count that cannot be sent right away (offline, blocked or slow) is dropped too, never queued or retried.
- **What it can show:** totals by day, version and kind of event, from the devices that turned it on, approximately. It cannot show who you are, how many people use Classic Cart Studio, or what any one person did.
- **Not collected:** nothing from the NES Classic itself, and nothing on a game's picture or sound.
- Agreeing to the Installation Terms, visiting the website or restoring a backup is not consent to these counts, and the counts are not consent to advertising. There is no advertising.

### What continues when Usage Reporting is off

| | When Usage Reporting is off |
|---|---|
| **1. Optional usage reports** (Classic Cart Studio's own counts, above) | Off. None are sent, and the Windows app makes no remote request for them. |
| **2. Cloudflare Web Analytics on the website** (visits, page views, referring sites, browser and device type, page-load performance; it is not in the Windows app) | Continues on the website, because the Usage Reporting choice does not cover it. A content or privacy blocker can block it without breaking the website. |
| **3. Delivering the website and downloads** (GitHub Pages and Cloudflare deliver the website; GitHub delivers downloads and the release information) | Continues whenever you load the website or download a release. These providers process connection information, such as your IP address, and may keep security and access records under their own terms. |

Classic Cart Studio has no compulsory usage reporting.

## Backups (.ccs files)

A `.ccs` backup is complete: every game it lists with its ROM file, plus the collection's loadouts, settings and your own artwork. A **Full Studio Backup** (Back Up Everything) holds every collection with their games, the details and artwork of your own games and your Game Settings; on the website it also holds the kept games and their saved games, and in the Windows app your saved games. Backups are made on your own device and saved where you choose. Classic Cart Studio never uploads a backup or any ROM file, and offers no way to send backups to other people. A restore adds what is missing and keeps a saved game that already exists and differs; it says what it kept.

Choices that belong to one device (the HOME Music choice and the usage reporting choice) are not part of any backup, so restoring a backup never changes them on another device.

## Open in Windows App (website → app)

When you choose **Open in Windows App**, the website sends the collection (or the `.ccs` package you picked) to the Windows app on your own computer at `127.0.0.1`, then opens the app through a `classiccartstudio://` link that carries only an identifier. Your browser may ask whether the website may reach apps on your device; that is the browser's local-network permission for exactly this step. The app accepts such offers only from the website's own address, keeps them for a short time, and asks you before opening one. Declining loses nothing: your collection stays in your browser, and **Back Up Collection** gives you a `.ccs` file to open instead. Nothing leaves your computer in this step.

## Classic Cart Studio for Windows

The app's interface is installed with it and served from your own computer; it does not load the website to run, and it does not include the website's Cloudflare Web Analytics.

**Stored on your PC** under `%LOCALAPPDATA%\ClassicCartStudio`:

- copies of ROM files you import, their artwork, and the index that describes them (your original files are never changed, moved or deleted);
- your collections, loadouts, favorites, snapshots and settings, saved so they survive an upgrade;
- the game player's saves (cartridge saves and the four save slots per game), Game Settings, Play on PC Settings and controller setups, and a dated copy of anything a backup restore replaced;
- the window's own browser profile;
- your usage reporting choice (`usage-reporting.json`), saved by setup or by Studio menu › Usage Reporting;
- collections and packages offered by the website or opened from a `.ccs` file that you have not yet opened or discarded (they expire after a day or less);
- a backup and a journal for every preparation run, so a run can be undone;
- logs of startup, requests, handoffs and errors (never passwords or keys), and the window size and position.

The setup installs the Installation Terms, the GNU General Public License, the third-party notices and a description of the optional usage reports in the program folder (`legal\`). It keeps no record of your agreement. The only answer it saves is your usage reporting choice (listed above), and it sends nothing.

The app reads the Hakchi2 CE folder only when you ask it to (Check, Prepare) and examines only the files or folders you pick for import. It does not scan your disk.

**Network:** the app's local service (the Background Helper) listens only on `127.0.0.1` (your own computer). In this release it never connects to a console: its console features are turned off. Hakchi2 CE 3.9.3 is included with the app and verified by SHA-256 before use. The app contacts the internet only when you ask it to (**Check for Updates** opens the releases page in your browser). While **Usage Reporting** is on, the local service also sends its few counts, checked again against your choice, to `https://metrics.classiccartstudio.com` and nowhere else (see *Optional usage reporting*). While it is off, or when no choice is saved, the app makes no request for it. Backing up and restoring never use the network. The app never checks for or installs updates by itself.

**Authorization:** the app's window talks to the local service with a session created when the app starts and discarded when it closes. A website in your normal browser can only hand over a collection or a package as described above, and only from `https://classiccartstudio.com`.

## Advertising

There is no advertising in this release, and no advertising network or tracking for advertising. If limited advertising is ever added to the website or the app, this notice will first describe it and any choices it needs. Agreeing to the Installation Terms, or choosing to send usage reports, is not consent to advertising.

## Downloads and updates

The website shows the latest release (version, size, SHA-256) so you can verify a download. Updates are installed only by you, by running a newer installer; your data folder is kept.

## Storage estimate

The storage figures in the loadout editor are estimates for a stock 2018 NES Classic Edition. They are not read from your console and are not personal data.

## Removal

Uninstalling Classic Cart Studio (Windows Settings → Apps) removes the program and keeps the data folder above, so your imported ROM files, collections and usage reporting choice survive a reinstall. Delete `%LOCALAPPDATA%\ClassicCartStudio` yourself to remove them. Clear the site's data in your browser (and remove the installed app) to remove what the website kept.

## Contact

Questions and reports: the repository's issue tracker at https://github.com/Rance9/Classic-Cart-Studio/issues (public). For security matters, see `SECURITY.md`.
