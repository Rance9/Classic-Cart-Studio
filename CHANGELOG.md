# Changelog

## 0.3.0 — 2026-10-11

- **Classic Cart Studio for Windows, modernized.** The window is now a WPF application (still .NET Framework 4.8, so nothing extra to install) with its own dark title bar: the Classic Cart Studio icon, the name, a small red accent, a discreet Studio menu (Fluent icons) and the standard minimize / maximize / close controls with Windows 11 Snap Layouts, rounded corners and a dark system frame. There is no menu bar and no status bar: the website gets the whole window below a 36 px title bar.
- Branded loading state while the backend starts and the website loads (pixel-block progress; static when Windows animations are off), a redesigned recovery screen when the website cannot be reached (Try Again, Check Connection, View Details, Diagnostics, Exit), and plain-language dialogs instead of system message boxes.
- About: compact window with the version, backend and WebView2 versions, links (ClassicCartStudio.com, ClassicCartStudio.com/app, source, license and notices) and the independent-project disclaimer. Diagnostics: status rows with colored indicators (application, WebView2 runtime, website, backend, compatibility, data folder, log), the raw backend answers, Restart backend, Open log, Open data folder, Copy report.
- Installer: Classic Cart Studio wizard images (dark, from the application icon, 100 % and 200 % versions), welcome and finish text in the product's voice, the Ready page skipped (the per-user install has nothing to confirm). Same installation folder, data folder, identifiers and nesstudio:// link, so 0.1.x and 0.2.x upgrade in place and keep their data.
- Behavior preserved: live website, backend started and supervised by the window, desktop session for the approved origin only, single instance, nesstudio:// handoff, durable desktop state, navigation filtering, clean shutdown. Links in the application point at Rance9/Classic-Cart-Studio and ClassicCartStudio.com/app.

## Website — 2026-10-11 (after 0.2.1; no new installer)

- New Classic Cart Studio logo: a gamepad silhouette (D-pad and two red buttons) on a dark tile with a red stripe, with hand-drawn 16, 24 and 32 px versions so it stays readable in browser tabs and the taskbar (no console brand, no character). It is the application icon, the website favicon set, the hero title mark, the welcome and guide header mark and the /app page mark. The website now has a favicon set (favicon.ico, 32 px PNG, apple-touch-icon) and a dark theme color for browser tabs.
- Content: the "not tested on this console" / "not verified with this console" caveats are gone from the compatibility evidence, the controller warnings, the tray, the guide and the backend; compatibility notes read "documentation and community reports".
- Performance: the game list is now windowed. Only the rows near the viewport exist in the page (about 20 to 40 at a time, in list and grid view), whatever the library size; the rest is represented by measured spacer heights. With the Extended Library on (3,056 titles) a window resize no longer re-lays out thousands of rows; the toolbar no longer blurs what scrolls under it; the Install button's glow pulses on the compositor instead of repainting a shadow; animations and transitions pause while the window edge is being dragged; on wide screens the columns have explicit widths so the painted rows settle the layout. Letter jumps, Select all visible, sorting, selection and the details dialog work as before.
- Fixes: the hero count follows the Extended Library switch (773 curated, or 3,056 with the extended versions); the "i" button next to Extended Library in Filter & Sort is styled (it was an unstyled gray box); when Search & Tools is opened on a narrower screen every line in it is centered (search, controls, stats and connection, active filters, letters, storage) and a "Hide Search & Tools" button closes it again.
- The NES Classic storage pill never shares a line with the letter strip: when the two would touch, the pill moves to its own centered row directly below the search-to-connection row (measured, at any width).
- Phones: the letter strip wraps instead of being cut off, the stats line is shown (it was hidden by the phone layout), the storage pill wraps on its own row, and the character parade wraps into rows inside the screen instead of being clipped at the edges. Nothing bleeds past the left or right edge at any width (checked from 360 to 1366 px).
- The Nintendo Entertainment System wordmark in the hero is drawn at its natural pixel size (it was scaled to 80 %, which blurred the pixel art at 100 % zoom).
- The B pixel art on the Tray button is the same size as the A on Install on NES Classic.
- The welcome screen shows on every start by default; a **Show this welcome on every start** box on it (checked by default) turns it off, and the choice is kept (and mirrored in the desktop application's durable state).
- The Active area stays visible when nothing is active and says so (No filters · curated library · Title A–Z) instead of disappearing.
- Clear all (in the Active filters row) now clears everything shown there, the Extended Library switch included, and a click anywhere on a chip counts (it ignored clicks on the chip's text and did not turn the Extended Library off).
- The Active filters (chips) share the letter row: letters on the left, Active on the right when both fit; when they do not, Active moves below the letters and both are centered. Measured, at any width.
- The console and controller drawings in the hero stay visible down to tablet widths (they used to disappear below 1100 px); the count on the Tray button is a quiet gray chip instead of a red badge.
- The NES Classic storage estimate (meter, estimated game space, to-install figure) is shown in the Collection Tray only; it no longer sits in the toolbar.
- **One path.** The website no longer connects to the backend on your computer at all: no connection pill, no Connect to Your Computer dialog, no pairing code, no local-network permission on page load. The website is for browsing and building; Install on NES Classic, Load Existing Collection (games already on this computer) and Add Your Own Games open **Continue in Classic Cart Studio for Windows**, which opens the app with your selections (or the app alone when nothing is selected) or sends you to ClassicCartStudio.com/app when it is not installed. Inside the app the status pill appears only when the backend needs attention.
- Repository renamed to **Rance9/Classic-Cart-Studio** (GitHub redirects the old name); links in the website, the README, the installer and the application point at the new name.
- Search & Tools section: the row that starts with the search box and ends with the connection pill never wraps. The page measures whether it fits on one line; the moment it would need a second line, the section collapses on its own, and opening it at such a width shows an aligned layout (search box across the full width, Filter & Sort / Select / List-Grid as three equal buttons, stats and the connection pill on one line). Widen the window again and the one-line layout returns. The top bar centers its rows whenever it wraps, at any width.

## 0.2.1 — 2026-10-11

- Installer: when upgrading from the 0.1.x companion, the Start-menu group and shortcuts are renamed to Classic Cart Studio and the old companion shortcuts are removed (0.2.0 kept the old group name and left the old shortcuts next to the new ones). No application or website changes.

## 0.2.0 — 2026-10-11

- **Classic Cart Studio** is the product's name (website, application window, Start menu, installer, Installed Apps, guide, README). The data folder `%LOCALAPPDATA%\NESClassicCollectionStudio`, the installer identity, the backend executable name and the `nesstudio://` link stay as they were, so this release upgrades the earlier companion in place and keeps everything you imported or saved.
- **Classic Cart Studio for Windows**: one application window (Microsoft Edge WebView2) that shows the live website and starts, supervises and stops the backend itself. No companion status window, no pairing step inside the application; the backend accepts the application's own session for the website origin only. Window size and position are remembered, one instance at a time (a second launch brings the window forward), high-DPI aware, a recovery screen when the website cannot be reached, a Studio menu with Diagnostics, the data folder, the releases page and About.
- **Continue in Desktop App** on the website (My Collection menu, Install on NES Classic, and when a local file operation needs the computer): the current collection is handed to the application on this computer through the backend (exact origin, bounded, idempotent), the application is opened through the `nesstudio://` link, and it asks before opening the collection; an open collection is kept as a snapshot. Exact ROM versions and factory-game choices are preserved. Fallbacks: install instructions when the application is not installed, and Download Collection & Open in App (the existing collection file).
- **Your Collection Is Ready** after a handoff: Find My Game Files (folder, files or ZIP archives through the existing importer), Review Collection, Do This Later.
- Durable desktop state: the application's collection, favorites, snapshots and settings are mirrored into `desktop-state.json` through the backend, so they survive a WebView2 profile reset and application upgrades.
- Backend: `/api/version` lists capabilities so the website can tell a compatible, outdated or missing backend apart; new desktop-only endpoints (handoff inbox, desktop state, shutdown); `--headless` mode for the application host. The legacy status window remains available when the backend is started on its own.
- Website: product name in the hero, title and metadata; Help & User Guide chapters Website vs Desktop App, Installing Classic Cart Studio and the new Continuing in the Desktop App; PRIVACY.md and THIRD_PARTY_NOTICES.md updated for WebView2 and the handoff. Installer file name is now `ClassicCartStudio-Setup-<version>.exe`.

## 0.1.6 — 2026-10-10

- Website now lives at **https://classiccartstudio.com/** (GitHub Pages custom domain; the old github.io address redirects there). The companion allows the new origin (and www) in addition to the github.io origin, so pairing works on the new address; links in the companion window, the installer and the README point at the new domain.
- Top bar: every button has the same height; when the bar wraps on narrower windows the buttons are centered on every row; the title list never scrolls sideways or bleeds past the right edge at any width (fixed column layout between 641 and 1000 px).

## 0.1.5 — 2026-10-10

- Website: toolbar buttons recolored (Save / Load matches My Collection; Tray is blue with the red B; Install on NES Classic is green with the red A); identity and emulator pills in the list are faint so the titles and art lead; a collapsible **Search & Tools** section holds search, Filter & Sort, Select, List/Grid, stats, the connection pill, the alphabet strip and the storage chip (collapsed on narrow screens until opened; on wide screens it folds away when you scroll into the list and returns at the top); a real phone layout (stacked task buttons, the list becomes a card list, no sideways scrolling anywhere, dialogs fit the screen).
- Help & User Guide: new showcase layout with a sprite band and chapter number on every chapter, grouped chapter list, reading progress bar, equal-height step cards on a dashed path (a timeline on phones), styled callouts, disclosures and tables, previous/next chapter cards, and ← / → keys to move between chapters.
- US English throughout the website, the guide, the companion and the repository text.
- Companion 0.1.5: file-picker replies use the `canceled` key (the website accepts both spellings, so a 0.1.4 companion keeps working); dropped client connections no longer print tracebacks in the companion log.

## 0.1.4 — 2026-10-10

- Website: compact hero (about 140 px) that stays on screen together with the toolbar; the duplicate brand block in the toolbar is gone; "How It Works" is now **Help & User Guide** (book icon, chapter count); one **Filter & Sort** dialog holds every filter, the sort order and the Extended Library switch, with Apply / Cancel / Reset, staged changes, an active-filter badge and chips; the toolbar is grouped by task; the search icon no longer overlaps typed text; no page-level horizontal scroll from 360 px to 3440 px wide (also at 125 % zoom).
- Connection: the website reports **Connected** as soon as the companion answers the version handshake (about 10 ms on a warm companion instead of 4 s); the console-setup check (Hakchi2 CE, driver, console) runs in the background with bounded timeouts and shows "Checking console setup…" / "Setup needs attention"; states shown: Checking for companion…, Companion not running, Permission needed, Waiting for approval, Connected.
- Companion 0.1.4: `/api/status` answers from a cached snapshot immediately and refreshes diagnostics in a background thread (`?wait=` for callers that need a fresh result); every probe is time-limited (process list 10 s, device enumeration 12 s, driver query 25 s); file hashes and version lookups are cached by size and modification time; the status payload carries the measured timings. Protocol stays 1: the 0.1.3 companion still works with this website (its status call is slower and shows no timings); updating is recommended.
- Storage: the header chip reads "NES Classic · estimated game space ~261 MB" and names its source (a reference estimate for a stock 2018 console, a read-only measurement, or unknown); the collection tray and footer stats use the same wording; no visitor free space is ever claimed and no private path is published.
- Legal: new guide chapter **Legal, Privacy & Disclaimers**, `PRIVACY.md`, `THIRD_PARTY_NOTICES.md`, a **Legal & Privacy** link in the footer and links from the README.

## 0.1.3 — 2026-10-10

- Restored the approved visual presentation on the website and in the companion's bundled site: the Nintendo hero and 27-character parade, the footer character roster with origins, the firmware interface icons and wordmark, and the game box art in grid, list, details and the Collection Tray (745 of 773 curated games). The public build no longer substitutes generic artwork or strips art references; ROM, console-image, secret and private-path exclusions are unchanged.
- Install button keeps its glow in every state; hover lift, glow and sheen on all buttons.

## 0.1.2 — 2026-10-10

- How It Works guide: content pass across all 24 chapters (shorter, direct wording that names Hakchi2 CE, FEL mode, mappers and hmods; stale artwork note removed), step boxes no longer stretch to the tallest step, verification command shown as a code block.

## 0.1.1 — 2026-10-10

- Installer: the post-uninstall note is no longer shown during a silent uninstall (`/VERYSILENT`), which blocked scripted removal in 0.1.0.
- Website: requests to the companion declare the loopback target address space (Microsoft Edge / Chrome Local Network Access); the connection dialog explains the browser's local-network permission when the companion cannot be reached.

## 0.1.0 — 2026-10-10

First public release.

- Website (GitHub Pages): the complete Studio — curated library of 773 North American releases plus the extended index, version chips, Technical Details, collections with save/load/snapshots, How It Works guide, original pixel art.
- Windows companion (per-user installer): loopback server with CORS pairing for the public website, local-library reading, bulk import of game files and folders (ZIP archives, exact-content identification, duplicates, safety limits), personal games with artwork, collection preparation for Hakchi2 CE 3.9.3 (verified download), console USB driver setup, status window, `nesstudio://` link.
- Honest limits: installing on the console is done by Hakchi2 CE under the user's control; this version prepares everything on the PC and does not write to the console. The installer is not code-signed; the SHA-256 and a build-provenance attestation are published instead.
